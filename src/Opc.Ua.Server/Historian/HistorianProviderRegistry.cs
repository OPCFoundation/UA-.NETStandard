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
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Identifies one registration contributed by a prepared NodeManager generation.
    /// </summary>
    /// <param name="Kind">The registration scope.</param>
    /// <param name="NodeId">The exact node identifier, when the scope is node-specific.</param>
    /// <param name="NamespaceUri">The namespace URI, when the scope is namespace-specific.</param>
    /// <param name="Provider">The historian provider.</param>
    /// <param name="OwnsProvider">Whether the registration owner owns provider disposal.</param>
    internal readonly record struct HistorianProviderRegistration(
        HistorianProviderRegistrationKind Kind,
        NodeId NodeId,
        string? NamespaceUri,
        IHistorianProvider Provider,
        bool OwnsProvider);

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
        /// Gets whether the registry retains disposal ownership, including providers no longer bound.
        /// </summary>
        bool OwnsProvider(IHistorianProvider provider);

        /// <summary>
        /// Gets whether the specified effective binding belongs to a lifecycle owner.
        /// </summary>
        bool IsOwnedBinding(
            HistorianProviderRegistrationKind kind,
            NodeId nodeId,
            string? namespaceUri,
            IHistorianProvider provider);

        /// <summary>
        /// Records a staged candidate that owns a provider not yet registered in the serving registry.
        /// </summary>
        void RetainStagedProviderOwnership(object owner, IHistorianProvider provider);

        /// <summary>
        /// Releases one staged provider claim and returns whether another candidate still claims the provider.
        /// </summary>
        bool ReleaseStagedProviderOwnership(object owner, IHistorianProvider provider);

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
        IHistorianNodeBindingRegistry,
        IHistorianNodeReplacementRegistry,
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
            RegisterForNode(nodeId, provider, ownsProvider: true);
        }

        /// <summary>
        /// Registers a provider for one node with an explicit lifetime and duplicate-key policy.
        /// </summary>
        /// <param name="nodeId">The exact node identifier.</param>
        /// <param name="provider">The historian provider.</param>
        /// <param name="ownsProvider">Whether this registry owns the provider's disposal lifetime.</param>
        /// <param name="rejectDuplicate">Whether an existing local binding must be rejected.</param>
        internal void RegisterForNode(
            NodeId nodeId,
            IHistorianProvider provider,
            bool ownsProvider,
            bool rejectDuplicate = false)
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
                if (rejectDuplicate && ResolveExactNodeCore(nodeId) != null)
                {
                    throw new InvalidOperationException($"A historian provider is already registered for node '{nodeId}'.");
                }
                m_nodes.TryGetValue(nodeId, out IHistorianProvider? previous);
                m_nodes[nodeId] = provider;
                m_nodeBindings.Remove(nodeId);
                m_nodeRegistrationOrder[nodeId] = ++m_registrationOrder;
                AddProvider(provider, ownsProvider);
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
            RegisterForNamespace(namespaceUri, provider, ownsProvider: true);
        }

        /// <summary>
        /// Registers a provider for a namespace with an explicit lifetime and duplicate-key policy.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="provider">The historian provider.</param>
        /// <param name="ownsProvider">Whether this registry owns the provider's disposal lifetime.</param>
        /// <param name="rejectDuplicate">Whether an existing local binding must be rejected.</param>
        internal void RegisterForNamespace(
            string namespaceUri,
            IHistorianProvider provider,
            bool ownsProvider,
            bool rejectDuplicate = false)
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
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(HistorianProviderRegistry));
                }
                if (rejectDuplicate && ResolveNamespaceCore(namespaceUri) != null)
                {
                    throw new InvalidOperationException(
                        $"A historian provider is already registered for namespace '{namespaceUri}'.");
                }
                m_namespaces.TryGetValue(namespaceUri, out IHistorianProvider? previous);
                m_namespaces[namespaceUri] = provider;
                m_namespaceRegistrationOrder[namespaceUri] = ++m_registrationOrder;
                AddProvider(provider, ownsProvider);
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
        /// <param name="provider">The historian provider.</param>
        /// <param name="ownsProvider">Whether this registry owns the provider's disposal lifetime.</param>
        /// <param name="rejectDuplicate">Whether an existing local binding must be rejected.</param>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
        internal void RegisterDefault(
            IHistorianProvider provider,
            bool ownsProvider,
            bool rejectDuplicate = false)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(HistorianProviderRegistry));
                }
                if (rejectDuplicate && ResolveDefaultCore() != null)
                {
                    throw new InvalidOperationException("A default historian provider is already registered.");
                }
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
                RejectPendingReplacement(nodeId);
                bool removed = m_nodes.TryGetValue(nodeId, out IHistorianProvider? provider);
                if (removed)
                {
                    m_nodes.Remove(nodeId);
                    m_nodeBindings.Remove(nodeId);
                    m_nodeRegistrationOrder.Remove(nodeId);
                }
                if (m_ownedNodes.TryGetValue(nodeId, out List<OwnedProviderBinding>? owned))
                {
                    m_ownedNodes.Remove(nodeId);
                    RemoveOwnedScope(owned, registration =>
                        registration.Kind == HistorianProviderRegistrationKind.Node &&
                        registration.NodeId == nodeId);
                    removed = true;
                }
                if (provider != null)
                {
                    RebuildProviderSet(provider);
                }
                return removed;
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
                bool removed = m_namespaces.TryGetValue(namespaceUri, out IHistorianProvider? provider);
                if (removed)
                {
                    m_namespaces.Remove(namespaceUri);
                    m_namespaceRegistrationOrder.Remove(namespaceUri);
                }
                if (m_ownedNamespaces.TryGetValue(namespaceUri, out List<OwnedProviderBinding>? owned))
                {
                    m_ownedNamespaces.Remove(namespaceUri);
                    RemoveOwnedScope(owned, registration =>
                        registration.Kind == HistorianProviderRegistrationKind.Namespace &&
                        string.Equals(registration.NamespaceUri, namespaceUri, StringComparison.Ordinal));
                    removed = true;
                }
                if (provider != null)
                {
                    RebuildProviderSet(provider);
                }
                return removed;
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
                RemoveOwnedScope(m_ownedDefaults, static registration =>
                    registration.Kind == HistorianProviderRegistrationKind.Default);
                m_ownedDefaults.Clear();
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
                    AddProvider(registration.Provider, registration.OwnsProvider);
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
                        entry.Value,
                        m_ownedProviders.Contains(entry.Value)));
                }
                foreach (KeyValuePair<string, IHistorianProvider> entry in m_namespaces)
                {
                    registrations.Add(new HistorianProviderRegistration(
                        HistorianProviderRegistrationKind.Namespace,
                        NodeId.Null,
                        entry.Key,
                        entry.Value,
                        m_ownedProviders.Contains(entry.Value)));
                }
                if (m_default is not null)
                {
                    registrations.Add(new HistorianProviderRegistration(
                        HistorianProviderRegistrationKind.Default,
                        NodeId.Null,
                        null,
                        m_default,
                        m_ownedProviders.Contains(m_default)));
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
        /// Transfers disposal ownership of the published registrations to the serving registry.
        /// </summary>
        internal void TransferProviderOwnership(HistorianProviderRegistration[] registrations)
        {
            lock (m_lock)
            {
                foreach (HistorianProviderRegistration registration in registrations)
                {
                    m_ownedProviders.Remove(registration.Provider);
                }
            }
        }

        /// <inheritdoc/>
        bool IHistorianOwnedProviderRegistry.OwnsProvider(IHistorianProvider provider)
        {
            lock (m_lock)
            {
                return m_ownedProviders.Contains(provider);
            }
        }

        /// <inheritdoc/>
        bool IHistorianOwnedProviderRegistry.IsOwnedBinding(
            HistorianProviderRegistrationKind kind,
            NodeId nodeId,
            string? namespaceUri,
            IHistorianProvider provider)
        {
            lock (m_lock)
            {
                return kind switch
                {
                    HistorianProviderRegistrationKind.Node =>
                        !nodeId.IsNull &&
                        TryGetLatestOwnedBinding(m_ownedNodes, nodeId, out IHistorianProvider? ownedNode,
                            out long ownedNodeOrder) &&
                        ReferenceEquals(ownedNode, provider) &&
                        (!m_nodeRegistrationOrder.TryGetValue(nodeId, out long nodeOrder) ||
                            ownedNodeOrder > nodeOrder),
                    HistorianProviderRegistrationKind.Namespace =>
                        !string.IsNullOrEmpty(namespaceUri) &&
                        TryGetLatestOwnedBinding(m_ownedNamespaces, namespaceUri, out IHistorianProvider? ownedNamespace,
                            out long ownedNamespaceOrder) &&
                        ReferenceEquals(ownedNamespace, provider) &&
                        (!m_namespaceRegistrationOrder.TryGetValue(namespaceUri, out long namespaceOrder) ||
                            ownedNamespaceOrder > namespaceOrder),
                    HistorianProviderRegistrationKind.Default =>
                        TryGetOwnedDefault(out IHistorianProvider? ownedDefault, out long ownedDefaultOrder) &&
                        ReferenceEquals(ownedDefault, provider) &&
                        (m_default == null || ownedDefaultOrder > m_defaultRegistrationOrder),
                    _ => false
                };
            }
        }

        /// <inheritdoc/>
        void IHistorianOwnedProviderRegistry.RetainStagedProviderOwnership(
            object owner,
            IHistorianProvider provider)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            lock (m_lock)
            {
                if (!m_stagedProviderOwners.TryGetValue(provider, out HashSet<object>? owners))
                {
                    m_stagedProviderOwners.Add(provider, owners = new HashSet<object>(RefEqualityComparer.Default));
                }
                owners.Add(owner);
            }
        }

        /// <inheritdoc/>
        bool IHistorianOwnedProviderRegistry.ReleaseStagedProviderOwnership(
            object owner,
            IHistorianProvider provider)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            lock (m_lock)
            {
                if (!m_stagedProviderOwners.TryGetValue(provider, out HashSet<object>? owners))
                {
                    return false;
                }
                owners.Remove(owner);
                if (owners.Count != 0)
                {
                    return true;
                }
                m_stagedProviderOwners.Remove(provider);
                return false;
            }
        }

        /// <summary>
        /// Relinquishes providers whose disposal lifetime belongs to the serving registry.
        /// </summary>
        /// <param name="sharedRegistry">The serving registry that may retain candidate providers.</param>
        /// <param name="owner">The candidate owner releasing its staged provider claims.</param>
        internal void ExcludeSharedProviderOwnership(
            IHistorianProviderRegistry sharedRegistry,
            object owner)
        {
            if (sharedRegistry is null)
            {
                throw new ArgumentNullException(nameof(sharedRegistry));
            }
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            var sharedProviders = new HashSet<IHistorianProvider>();
            ArrayOf<IHistorianProvider> registeredProviders = sharedRegistry.Providers;
            for (int index = 0; index < registeredProviders.Count; index++)
            {
                sharedProviders.Add(registeredProviders[index]);
            }

            IHistorianProvider[] ownedProviders;
            lock (m_lock)
            {
                ownedProviders = [.. m_ownedProviders];
            }
            if (sharedRegistry is IHistorianOwnedProviderRegistry sharedOwner)
            {
                foreach (IHistorianProvider provider in ownedProviders)
                {
                    if (sharedOwner.OwnsProvider(provider))
                    {
                        sharedProviders.Add(provider);
                    }
                    if (sharedOwner.ReleaseStagedProviderOwnership(owner, provider))
                    {
                        sharedProviders.Add(provider);
                    }
                }
            }
            lock (m_lock)
            {
                m_ownedProviders.ExceptWith(sharedProviders);
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
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
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

        /// <summary>
        /// Reserves a replacement before fluent setup changes attributes, restoring the prior owner if setup fails.
        /// </summary>
        private NodeReplacement BeginNodeReplacement(BaseVariableState node, IHistorianProvider provider)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
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
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(HistorianProviderRegistry));
                }
                if (ResolveExactNodeCore(nodeId) != null || m_pendingReplacements.ContainsKey(nodeId))
                {
                    binding = null;
                    return false;
                }
                var registration = new NodeBinding(this, nodeId, node);
                m_nodes.Add(nodeId, provider);
                m_nodeBindings.Add(nodeId, registration);
                m_nodeRegistrationOrder[nodeId] = ++m_registrationOrder;
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
                        m_nodeRegistrationOrder.Remove(nodeId);
                        RebuildProviderSet(provider);
                    }
                }
            }
        }

        /// <summary>
        /// Retains ownership across asynchronous metadata cleanup and excludes a replacement until it completes.
        /// </summary>
        private async Task ReleaseNodeBindingAsync(NodeId nodeId, NodeBinding binding, Func<ValueTask> cleanup)
        {
            while (true)
            {
                Task? pending = null;
                lock (m_lock)
                {
                    if (m_pendingReplacements.TryGetValue(nodeId, out NodeBinding? replacement))
                    {
                        pending = replacement.Completion.Task;
                    }
                    else if (!m_nodeBindings.TryGetValue(nodeId, out NodeBinding? current) ||
                        !ReferenceEquals(current, binding))
                    {
                        return;
                    }
                    else
                    {
                        m_pendingReplacements.Add(nodeId, binding);
                    }
                }
                if (pending == null)
                {
                    break;
                }
                await pending.ConfigureAwait(false);
            }
            try
            {
                await cleanup().ConfigureAwait(false);
            }
            finally
            {
                lock (m_lock)
                {
                    m_pendingReplacements.Remove(nodeId);
                    try
                    {
                        ReleaseNodeBinding(nodeId, binding);
                    }
                    finally
                    {
                        binding.Completion.TrySetResult(true);
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
            try
            {
                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        m_pendingReplacements.Remove(nodeId);
                        if (previousBinding?.IsDisposed == true)
                        {
                            previousBinding.RestoreAttributes();
                        }
                        else
                        {
                            current.RestoreAttributes();
                        }
                        if (commit)
                        {
                            throw new ObjectDisposedException(nameof(HistorianProviderRegistry));
                        }
                        return;
                    }
                    if (!m_pendingReplacements.TryGetValue(nodeId, out NodeBinding? active) ||
                        !ReferenceEquals(active, current))
                    {
                        if (commit)
                        {
                            throw new InvalidOperationException(
                                "The historian replacement reservation is no longer current.");
                        }
                        return;
                    }
                    m_pendingReplacements.Remove(nodeId);
                    if (commit)
                    {
                        m_nodes[nodeId] = provider;
                        m_nodeBindings.Remove(nodeId);
                        m_nodeRegistrationOrder[nodeId] = ++m_registrationOrder;
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
                        m_nodeRegistrationOrder.Remove(nodeId);
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
            finally
            {
                current.Completion.TrySetResult(true);
            }
        }

        /// <summary>
        /// Rejects competing mutations before they can alter an in-progress replacement.
        /// </summary>
        private void RejectPendingReplacement(NodeId nodeId)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(HistorianProviderRegistry));
            }
            if (m_pendingReplacements.ContainsKey(nodeId))
            {
                throw new InvalidOperationException(
                    "A historian replacement is already being configured for this node.");
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

        /// <summary>
        /// The identity of one detachable exact-node registration.
        /// </summary>
        private sealed class NodeBinding : IHistorianNodeBinding
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
            /// Whether the original owner released this registration, including during replacement setup.
            /// </summary>
            internal bool IsDisposed { get; set; }

            /// <summary>
            /// Signals that a pending replacement or metadata release no longer excludes this node.
            /// </summary>
            internal TaskCompletionSource<bool> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (m_releaseLock)
                {
                    if (m_release == null)
                    {
                        m_registry.ReleaseNodeBinding(m_nodeId, this);
                    }
                }
            }

            /// <inheritdoc/>
            public ValueTask ReleaseAsync(Func<ValueTask> cleanup)
            {
                if (cleanup == null)
                {
                    throw new ArgumentNullException(nameof(cleanup));
                }
                TaskCompletionSource<bool> completion;
                bool start;
                lock (m_releaseLock)
                {
                    start = m_release == null;
                    completion = m_release ??=
                        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                if (start)
                {
                    _ = CompleteReleaseAsync(cleanup, completion);
                }
                return new ValueTask(completion.Task);
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

            /// <summary>
            /// Publishes the first release's completed outcome without running application cleanup during acquisition.
            /// </summary>
            private async Task CompleteReleaseAsync(Func<ValueTask> cleanup, TaskCompletionSource<bool> completion)
            {
                try
                {
                    await m_registry.ReleaseNodeBindingAsync(m_nodeId, this, cleanup).ConfigureAwait(false);
                    completion.TrySetResult(true);
                }
                catch (Exception failure)
                {
                    completion.TrySetException(failure);
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

            /// <summary>Serializes acquisition of the shared release outcome.</summary>
            private readonly Lock m_releaseLock = new();

            /// <summary>The first metadata release and its retained outcome.</summary>
            private TaskCompletionSource<bool>? m_release;
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

        /// <summary>
        /// Removes one serving scope from the affected owners without changing their other registrations.
        /// </summary>
        private void RemoveOwnedScope(
            List<OwnedProviderBinding> bindings,
            Predicate<HistorianProviderRegistration> matches)
        {
            foreach (OwnedProviderBinding binding in bindings)
            {
                if (!m_ownedRegistrations.TryGetValue(binding.Owner, out OwnedRegistrationSet? registrations))
                {
                    continue;
                }
                HistorianProviderRegistration[] remaining =
                    Array.FindAll(registrations.Registrations, registration => !matches(registration));
                if (remaining.Length == 0)
                {
                    m_ownedRegistrations.Remove(binding.Owner);
                }
                else
                {
                    m_ownedRegistrations[binding.Owner] = new OwnedRegistrationSet(remaining);
                }
            }
            foreach (OwnedProviderBinding binding in bindings)
            {
                RebuildProviderSet(binding.Provider);
            }
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

        /// <summary>Identities of detachable bindings, distinct from provider identity.</summary>
        private readonly NodeIdDictionary<NodeBinding> m_nodeBindings = [];

        /// <summary>Unpublished replacements that exclude competing mutations for the same node.</summary>
        private readonly NodeIdDictionary<NodeBinding> m_pendingReplacements = [];

        private readonly Dictionary<string, IHistorianProvider> m_namespaces = new(StringComparer.Ordinal);
        private readonly NodeIdDictionary<long> m_nodeRegistrationOrder = [];
        private readonly Dictionary<string, long> m_namespaceRegistrationOrder = new(StringComparer.Ordinal);
        private readonly NodeIdDictionary<List<OwnedProviderBinding>> m_ownedNodes = [];
        private readonly Dictionary<string, List<OwnedProviderBinding>> m_ownedNamespaces =
            new(StringComparer.Ordinal);
        private readonly List<OwnedProviderBinding> m_ownedDefaults = [];
        private readonly Dictionary<object, OwnedRegistrationSet> m_ownedRegistrations =
            new(RefEqualityComparer.Default);
        private readonly Dictionary<IHistorianProvider, HashSet<object>> m_stagedProviderOwners =
            new(RefEqualityComparer.Default);
        private readonly HashSet<IHistorianProvider> m_providers = [];
        private readonly HashSet<IHistorianProvider> m_ownedProviders = [];
        private IHistorianProvider? m_default;
        /// <summary>Whether shutdown invalidated all registrations and pending replacements.</summary>
        private bool m_disposed;
        private long m_defaultRegistrationOrder;
        private long m_registrationOrder;

        private sealed record OwnedRegistrationSet(HistorianProviderRegistration[] Registrations);

        private sealed record OwnedProviderBinding(
            object Owner,
            IHistorianProvider Provider,
            long Order);
    }
}
