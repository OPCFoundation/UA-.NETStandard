/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
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
 *
 * The complete license agreement can be found at
 * http://opcfoundation.org/License/MIT/1.00/.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Stages candidate historian registrations and retains serving bindings during replacement readiness.
    /// </summary>
    internal sealed class StagedHistorianProviderRegistry : IHistorianProviderRegistry, IDisposable
    {
        /// <summary>
        /// Creates a candidate registry layered over the current serving registry.
        /// </summary>
        /// <param name="namespaceTable">The candidate namespace table.</param>
        /// <param name="servingRegistry">The canonical server registry.</param>
        /// <param name="owner">The candidate identity that owns the buffered registrations.</param>
        internal StagedHistorianProviderRegistry(
            NamespaceTable namespaceTable,
            IHistorianProviderRegistry servingRegistry,
            object owner)
        {
            m_namespaceTable = namespaceTable ?? throw new ArgumentNullException(nameof(namespaceTable));
            m_candidateRegistry = new HistorianProviderRegistry(
                m_namespaceTable);
            m_servingRegistry = servingRegistry ?? throw new ArgumentNullException(nameof(servingRegistry));
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>
        /// Gets whether this candidate has any local registration.
        /// </summary>
        internal bool HasRegistrations => m_candidateRegistry.CaptureRegistrations().Length != 0;

        /// <summary>
        /// Gets whether the serving registry can atomically publish and remove candidate-owned bindings.
        /// </summary>
        internal bool SupportsOwnedRegistrations =>
            m_servingRegistry is IHistorianOwnedProviderRegistry;

        /// <inheritdoc/>
        public void RegisterForNode(NodeId nodeId, IHistorianProvider provider)
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                RemoveRetainedNodeBinding(nodeId);
                m_candidateRegistry.RegisterForNode(
                    nodeId, provider, OwnsProviderForCandidate(provider), rejectDuplicate: true);
                UpdateServingRegistrations();
            }
        }

        /// <inheritdoc/>
        public void RegisterForNamespace(string namespaceUri, IHistorianProvider provider)
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                RemoveRetainedNamespaceBinding(namespaceUri);
                m_candidateRegistry.RegisterForNamespace(
                    namespaceUri, provider, OwnsProviderForCandidate(provider), rejectDuplicate: true);
                UpdateServingRegistrations();
            }
        }

        /// <inheritdoc/>
        public void RegisterDefault(IHistorianProvider provider)
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                RemoveRetainedDefaultBinding();
                m_candidateRegistry.RegisterDefault(
                    provider, OwnsProviderForCandidate(provider), rejectDuplicate: true);
                UpdateServingRegistrations();
            }
        }

        /// <inheritdoc/>
        public bool UnregisterForNode(NodeId nodeId)
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                bool removed = m_candidateRegistry.UnregisterForNode(nodeId);
                m_retainedNodeIds.Remove(nodeId);
                UpdateServingRegistrations();
                return removed;
            }
        }

        /// <inheritdoc/>
        public bool UnregisterForNamespace(string namespaceUri)
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                bool removed = m_candidateRegistry.UnregisterForNamespace(namespaceUri);
                m_retainedNamespaceUris.Remove(namespaceUri);
                UpdateServingRegistrations();
                return removed;
            }
        }

        /// <inheritdoc/>
        public void ClearDefault()
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                m_candidateRegistry.ClearDefault();
                m_retainedDefault = false;
                UpdateServingRegistrations();
            }
        }

        /// <inheritdoc/>
        public IHistorianProvider? Resolve(NodeId nodeId)
        {
            lock (m_lock)
            {
                if (nodeId.IsNull)
                {
                    return null;
                }
                if (m_servingRegistry is not IHistorianOwnedProviderRegistry servingRegistry)
                {
                    return m_servingRegistry.Resolve(nodeId);
                }

                IHistorianProvider? provider = m_candidateRegistry.ResolveExactNode(nodeId) ??
                    servingRegistry.ResolveExactNode(nodeId);
                if (provider != null)
                {
                    return provider;
                }
                string? namespaceUri = m_namespaceTable.GetString(nodeId.NamespaceIndex);
                if (namespaceUri != null)
                {
                    provider = m_candidateRegistry.ResolveNamespace(namespaceUri) ??
                        servingRegistry.ResolveNamespace(namespaceUri);
                    if (provider != null)
                    {
                        return provider;
                    }
                }
                return m_candidateRegistry.ResolveDefault() ?? servingRegistry.ResolveDefault();
            }
        }

        /// <summary>
        /// Resolves a provider for fluent historization and retains serving bindings in this candidate.
        /// </summary>
        /// <param name="nodeId">The historized node identifier.</param>
        /// <returns>The provider selected by the candidate and serving scopes, if any.</returns>
        internal IHistorianProvider? ResolveForHistorization(NodeId nodeId)
        {
            lock (m_lock)
            {
                if (nodeId.IsNull)
                {
                    return null;
                }
                if (m_servingRegistry is not IHistorianOwnedProviderRegistry servingRegistry)
                {
                    return m_servingRegistry.Resolve(nodeId);
                }

                IHistorianProvider? provider = m_candidateRegistry.ResolveExactNode(nodeId);
                if (provider != null)
                {
                    return provider;
                }
                provider = servingRegistry.ResolveExactNode(nodeId);
                if (provider != null)
                {
                    RetainForCandidate(provider, nodeId, null);
                    return provider;
                }

                string? namespaceUri = m_namespaceTable.GetString(nodeId.NamespaceIndex);
                if (namespaceUri != null)
                {
                    provider = m_candidateRegistry.ResolveNamespace(namespaceUri);
                    if (provider != null)
                    {
                        return provider;
                    }
                    provider = servingRegistry.ResolveNamespace(namespaceUri);
                    if (provider != null)
                    {
                        RetainForCandidate(provider, NodeId.Null, namespaceUri);
                        return provider;
                    }
                }

                provider = m_candidateRegistry.ResolveDefault();
                if (provider != null)
                {
                    return provider;
                }
                provider = servingRegistry.ResolveDefault();
                if (provider != null)
                {
                    RetainForCandidate(provider, NodeId.Null, null);
                }
                return provider;
            }
        }

        /// <inheritdoc/>
        public ArrayOf<IHistorianProvider> Providers
        {
            get
            {
                lock (m_lock)
                {
                    ArrayOf<IHistorianProvider> serving = m_servingRegistry.Providers;
                    ArrayOf<IHistorianProvider> candidate = m_candidateRegistry.Providers;
                    var providers = new HashSet<IHistorianProvider>();
                    for (int index = 0; index < serving.Count; index++)
                    {
                        providers.Add(serving[index]);
                    }
                    for (int index = 0; index < candidate.Count; index++)
                    {
                        providers.Add(candidate[index]);
                    }
                    return [.. providers];
                }
            }
        }

        /// <summary>
        /// Publishes the complete candidate registration set after readiness succeeds.
        /// </summary>
        internal void Publish()
        {
            lock (m_lock)
            {
                EnsureMutable();
                HistorianProviderRegistration[] registrations =
                    m_candidateRegistry.CaptureRegistrations();
                if (registrations.Length != 0 || m_servingHasRegistrations)
                {
                    PublishOwnedRegistrations(registrations);
                }
                m_published = true;
            }
        }

        /// <summary>
        /// Withdraws this candidate's serving registrations when its generation leaves routing.
        /// </summary>
        internal bool Retire()
        {
            lock (m_lock)
            {
                if (m_retired)
                {
                    return false;
                }
                bool hadServingRegistrations = m_servingHasRegistrations;
                if (hadServingRegistrations)
                {
                    GetOwnedRegistry().RemoveOwnedRegistrations(m_owner);
                    m_servingHasRegistrations = false;
                }
                m_published = false;
                m_retired = true;
                return hadServingRegistrations;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                if (m_servingHasRegistrations)
                {
                    GetOwnedRegistry().RemoveOwnedRegistrations(m_owner);
                    m_servingHasRegistrations = false;
                }
                m_published = false;
                m_retired = true;
                m_candidateRegistry.ExcludeSharedProviderOwnership(m_servingRegistry, m_owner);
                m_candidateRegistry.Dispose();
                m_disposed = true;
            }
        }

        private void UpdateServingRegistrations()
        {
            if (m_published)
            {
                HistorianProviderRegistration[] registrations =
                    m_candidateRegistry.CaptureRegistrations();
                if (registrations.Length != 0 || m_servingHasRegistrations)
                {
                    PublishOwnedRegistrations(registrations);
                }
                return;
            }

            if (m_servingHasRegistrations ||
                m_retainedDefault ||
                m_retainedNodeIds.Count != 0 ||
                m_retainedNamespaceUris.Count != 0)
            {
                PublishOwnedRegistrations(CaptureRetainedRegistrations());
            }
        }

        private HistorianProviderRegistration[] CaptureRetainedRegistrations()
        {
            HistorianProviderRegistration[] registrations =
                m_candidateRegistry.CaptureRegistrations();
            var retained = new List<HistorianProviderRegistration>(registrations.Length);
            foreach (HistorianProviderRegistration registration in registrations)
            {
                bool isRetained = registration.Kind switch
                {
                    HistorianProviderRegistrationKind.Node =>
                        m_retainedNodeIds.Contains(registration.NodeId),
                    HistorianProviderRegistrationKind.Namespace =>
                        m_retainedNamespaceUris.Contains(registration.NamespaceUri!),
                    HistorianProviderRegistrationKind.Default => m_retainedDefault,
                    _ => false
                };
                if (isRetained)
                {
                    retained.Add(registration);
                }
            }
            return [.. retained];
        }

        private void PublishOwnedRegistrations(HistorianProviderRegistration[] registrations)
        {
            IHistorianOwnedProviderRegistry servingRegistry = GetOwnedRegistry();
            servingRegistry.ReplaceOwnedRegistrations(m_owner, registrations);
            m_servingHasRegistrations = registrations.Length != 0;
            if (registrations.Length != 0)
            {
                m_candidateRegistry.TransferProviderOwnership(registrations);
                var transferredProviders = new HashSet<IHistorianProvider>();
                foreach (HistorianProviderRegistration registration in registrations)
                {
                    if (registration.OwnsProvider && transferredProviders.Add(registration.Provider))
                    {
                        servingRegistry.ReleaseStagedProviderOwnership(m_owner, registration.Provider);
                    }
                }
            }
        }

        private void EnsureOwnerSupportForMutation()
        {
            if (m_servingRegistry is not IHistorianOwnedProviderRegistry)
            {
                throw new NotSupportedException(
                    "The serving historian registry does not support candidate-owned registrations.");
            }
        }

        private IHistorianOwnedProviderRegistry GetOwnedRegistry()
        {
            return m_servingRegistry as IHistorianOwnedProviderRegistry ??
                throw new NotSupportedException(
                    "The serving historian registry does not support candidate-owned registrations.");
        }

        private bool OwnsProviderForCandidate(IHistorianProvider provider)
        {
            if (((IHistorianOwnedProviderRegistry)m_candidateRegistry).OwnsProvider(provider))
            {
                return true;
            }
            if (m_servingRegistry is not IHistorianOwnedProviderRegistry servingOwner)
            {
                return false;
            }
            if (servingOwner.OwnsProvider(provider))
            {
                return false;
            }

            ArrayOf<IHistorianProvider> servingProviders = m_servingRegistry.Providers;
            for (int index = 0; index < servingProviders.Count; index++)
            {
                if (ReferenceEquals(servingProviders[index], provider))
                {
                    return false;
                }
            }
            servingOwner.RetainStagedProviderOwnership(m_owner, provider);
            return true;
        }

        private void RetainForCandidate(IHistorianProvider provider, NodeId nodeId, string? namespaceUri)
        {
            IHistorianOwnedProviderRegistry servingRegistry =
                (IHistorianOwnedProviderRegistry)m_servingRegistry;
            HistorianProviderRegistrationKind kind = !nodeId.IsNull
                ? HistorianProviderRegistrationKind.Node
                : namespaceUri is not null
                    ? HistorianProviderRegistrationKind.Namespace
                    : HistorianProviderRegistrationKind.Default;
            if (!servingRegistry.IsOwnedBinding(kind, nodeId, namespaceUri, provider))
            {
                return;
            }
            EnsureMutable();
            EnsureOwnerSupportForMutation();
            if (!nodeId.IsNull)
            {
                m_candidateRegistry.RegisterForNode(
                    nodeId, provider, ownsProvider: false, rejectDuplicate: true);
                m_retainedNodeIds.Add(nodeId);
            }
            else if (namespaceUri is not null)
            {
                m_candidateRegistry.RegisterForNamespace(
                    namespaceUri, provider, ownsProvider: false, rejectDuplicate: true);
                m_retainedNamespaceUris.Add(namespaceUri);
            }
            else
            {
                m_candidateRegistry.RegisterDefault(
                    provider, ownsProvider: false, rejectDuplicate: true);
                m_retainedDefault = true;
            }
            UpdateServingRegistrations();
        }

        private void RemoveRetainedNodeBinding(NodeId nodeId)
        {
            if (m_retainedNodeIds.Remove(nodeId))
            {
                m_candidateRegistry.UnregisterForNode(nodeId);
            }
        }

        private void RemoveRetainedNamespaceBinding(string namespaceUri)
        {
            if (m_retainedNamespaceUris.Remove(namespaceUri))
            {
                m_candidateRegistry.UnregisterForNamespace(namespaceUri);
            }
        }

        private void RemoveRetainedDefaultBinding()
        {
            if (m_retainedDefault)
            {
                m_candidateRegistry.ClearDefault();
                m_retainedDefault = false;
            }
        }

        private void EnsureMutable()
        {
            if (m_disposed || m_retired)
            {
                throw new ObjectDisposedException(nameof(StagedHistorianProviderRegistry));
            }
        }

        private readonly Lock m_lock = new();
        private readonly NamespaceTable m_namespaceTable;
        private readonly HistorianProviderRegistry m_candidateRegistry;
        private readonly IHistorianProviderRegistry m_servingRegistry;
        private readonly object m_owner;
        private readonly HashSet<NodeId> m_retainedNodeIds = [];
        private readonly HashSet<string> m_retainedNamespaceUris = new(StringComparer.Ordinal);
        private bool m_published;
        private bool m_servingHasRegistrations;
        private bool m_retainedDefault;
        private bool m_retired;
        private bool m_disposed;
    }
}
