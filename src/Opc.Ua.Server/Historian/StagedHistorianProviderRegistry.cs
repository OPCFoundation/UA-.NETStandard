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
    /// Buffers one candidate's historian registrations until its readiness callback succeeds.
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
                m_candidateRegistry.RegisterForNode(nodeId, provider);
                PublishIfReady();
            }
        }

        /// <inheritdoc/>
        public void RegisterForNamespace(string namespaceUri, IHistorianProvider provider)
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                m_candidateRegistry.RegisterForNamespace(namespaceUri, provider);
                PublishIfReady();
            }
        }

        /// <inheritdoc/>
        public void RegisterDefault(IHistorianProvider provider)
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                m_candidateRegistry.RegisterDefault(provider);
                PublishIfReady();
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
                PublishIfReady();
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
                PublishIfReady();
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
                PublishIfReady();
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
                if (registrations.Length != 0)
                {
                    PublishOwnedRegistrations(registrations);
                }
                m_published = true;
            }
        }

        /// <summary>
        /// Withdraws this candidate's serving registrations when its generation leaves routing.
        /// </summary>
        internal void Retire()
        {
            lock (m_lock)
            {
                if (m_retired)
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
                m_candidateRegistry.Dispose();
                m_disposed = true;
            }
        }

        private void PublishIfReady()
        {
            if (m_published)
            {
                HistorianProviderRegistration[] registrations =
                    m_candidateRegistry.CaptureRegistrations();
                if (registrations.Length != 0 || m_servingHasRegistrations)
                {
                    PublishOwnedRegistrations(registrations);
                }
            }
        }

        private void PublishOwnedRegistrations(HistorianProviderRegistration[] registrations)
        {
            GetOwnedRegistry().ReplaceOwnedRegistrations(m_owner, registrations);
            m_servingHasRegistrations = registrations.Length != 0;
            if (registrations.Length != 0)
            {
                m_candidateRegistry.TransferProviderOwnership();
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
        private bool m_published;
        private bool m_servingHasRegistrations;
        private bool m_retired;
        private bool m_disposed;
    }
}
