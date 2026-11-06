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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Stages candidate historian registrations and retains serving bindings during replacement readiness.
    /// </summary>
    internal sealed class StagedHistorianProviderRegistry :
        IHistorianProviderRegistry, IHistorianNodeBindingRegistry, IHistorianNodeReplacementRegistry, IDisposable
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

        /// <summary>
        /// Commits a node replacement while the candidate is still mutable and republishes its registration set.
        /// </summary>
        /// <param name="replacement">The candidate replacement transaction.</param>
        private void CommitNodeReplacement(IHistorianNodeReplacement replacement)
        {
            lock (m_lock)
            {
                EnsureMutable();
                replacement.Commit();
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
        public bool TryRegisterForNode(NodeId nodeId, IHistorianProvider provider, out IDisposable? binding)
        {
            return TryRegisterForNode(nodeId, null, provider, out binding);
        }

        /// <inheritdoc/>
        public bool TryRegisterForNode(NodeState node, IHistorianProvider provider, out IDisposable? binding)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            return TryRegisterForNode(node.NodeId, node, provider, out binding);
        }

        /// <inheritdoc/>
        IHistorianNodeReplacement IHistorianNodeReplacementRegistry.BeginNodeReplacement(
            BaseVariableState node,
            IHistorianProvider provider)
        {
            lock (m_lock)
            {
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                IHistorianNodeReplacement replacement =
                    ((IHistorianNodeReplacementRegistry)m_candidateRegistry).BeginNodeReplacement(node, provider);
                return new StagedNodeReplacement(this, replacement);
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

        /// <summary>
        /// Acquires a candidate-owned exact-node binding and updates any published candidate registration set.
        /// </summary>
        /// <param name="nodeId">The exact node to bind.</param>
        /// <param name="node">The node whose historian attributes belong to this binding, if available.</param>
        /// <param name="provider">The historian provider.</param>
        /// <param name="binding">The binding that owns the registration, or null if one already exists.</param>
        /// <returns>Whether a new binding was installed.</returns>
        private bool TryRegisterForNode(
            NodeId nodeId,
            NodeState? node,
            IHistorianProvider provider,
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
                EnsureMutable();
                EnsureOwnerSupportForMutation();
                RemoveRetainedNodeBinding(nodeId);
                IDisposable? candidateBinding;
                bool registered;
                if (node == null)
                {
                    registered = m_candidateRegistry.TryRegisterForNode(nodeId, provider, out candidateBinding);
                }
                else
                {
                    registered = m_candidateRegistry.TryRegisterForNode(node, provider, out candidateBinding);
                }
                if (!registered)
                {
                    binding = null;
                    UpdateServingRegistrations();
                    return false;
                }
                if (candidateBinding is not IHistorianNodeBinding scopedBinding)
                {
                    candidateBinding?.Dispose();
                    throw new InvalidOperationException(
                        "The historian registry returned a binding without scoped release support.");
                }

                try
                {
                    UpdateServingRegistrations();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    try
                    {
                        scopedBinding.Dispose();
                        UpdateServingRegistrations();
                    }
                    catch (Exception rollbackException) when (rollbackException is not OutOfMemoryException)
                    {
                        throw new AggregateException(
                            "Publishing and rolling back a staged historian binding both failed.",
                            exception,
                            rollbackException);
                    }
                    throw;
                }

                binding = new StagedNodeBinding(this, scopedBinding);
                return true;
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

        /// <summary>
        /// Refreshes published registrations unless this candidate has already left routing.
        /// </summary>
        private void RefreshServingRegistrations()
        {
            lock (m_lock)
            {
                if (!m_disposed && !m_retired)
                {
                    UpdateServingRegistrations();
                }
            }
        }

        /// <summary>
        /// Combines a primary cleanup failure with a subsequent registration-refresh failure.
        /// </summary>
        /// <param name="failure">The first failure, if any.</param>
        /// <param name="additionalFailure">The additional failure.</param>
        /// <returns>The failure to report.</returns>
        private static Exception CombineFailure(Exception? failure, Exception additionalFailure)
        {
            return failure == null
                ? additionalFailure
                : new AggregateException(failure, additionalFailure);
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

        /// <summary>
        /// A staged binding that refreshes candidate publication after its one owned release.
        /// </summary>
        private sealed class StagedNodeBinding : IHistorianNodeBinding
        {
            /// <summary>
            /// Creates a binding wrapper.
            /// </summary>
            /// <param name="owner">The candidate registry.</param>
            /// <param name="binding">The candidate registry's scoped binding.</param>
            internal StagedNodeBinding(
                StagedHistorianProviderRegistry owner,
                IHistorianNodeBinding binding)
            {
                m_owner = owner;
                m_binding = binding;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (m_releaseLock)
                {
                    if (m_release != null)
                    {
                        return;
                    }

                    Exception? failure = null;
                    try
                    {
                        m_binding.Dispose();
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        failure = exception;
                    }
                    try
                    {
                        m_owner.RefreshServingRegistrations();
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        failure = StagedHistorianProviderRegistry.CombineFailure(failure, exception);
                    }
                    if (failure != null)
                    {
                        ExceptionDispatchInfo.Capture(failure).Throw();
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
                Task release;
                lock (m_releaseLock)
                {
                    m_release ??= ReleaseCoreAsync(cleanup);
                    release = m_release;
                }
                return new ValueTask(release);
            }

            /// <summary>
            /// Releases the inner binding once and refreshes the published candidate registrations.
            /// </summary>
            /// <param name="cleanup">The metadata cleanup owned by this binding.</param>
            /// <returns>The completed release operation.</returns>
            private async Task ReleaseCoreAsync(Func<ValueTask> cleanup)
            {
                Exception? failure = null;
                try
                {
                    await m_binding.ReleaseAsync(cleanup).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failure = exception;
                }
                try
                {
                    m_owner.RefreshServingRegistrations();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failure = StagedHistorianProviderRegistry.CombineFailure(failure, exception);
                }
                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }

            /// <summary>
            /// Serializes disposal and memoizes the first asynchronous release.
            /// </summary>
            private readonly Lock m_releaseLock = new();

            /// <summary>
            /// The staged registry that republishes this binding's registration changes.
            /// </summary>
            private readonly StagedHistorianProviderRegistry m_owner;

            /// <summary>
            /// The scoped registration owned by the candidate registry.
            /// </summary>
            private readonly IHistorianNodeBinding m_binding;

            /// <summary>
            /// The first asynchronous release operation, if started.
            /// </summary>
            private Task? m_release;
        }

        /// <summary>
        /// A staged replacement that republishes after commit or rollback.
        /// </summary>
        private sealed class StagedNodeReplacement : IHistorianNodeReplacement
        {
            /// <summary>
            /// Creates a replacement wrapper.
            /// </summary>
            /// <param name="owner">The candidate registry.</param>
            /// <param name="replacement">The candidate registry's replacement transaction.</param>
            internal StagedNodeReplacement(
                StagedHistorianProviderRegistry owner,
                IHistorianNodeReplacement replacement)
            {
                m_owner = owner;
                m_replacement = replacement;
            }

            /// <inheritdoc/>
            public void Commit()
            {
                m_owner.CommitNodeReplacement(m_replacement);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Exception? failure = null;
                try
                {
                    m_replacement.Dispose();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failure = exception;
                }
                try
                {
                    m_owner.RefreshServingRegistrations();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failure = CombineFailure(failure, exception);
                }
                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }

            /// <summary>
            /// The staged registry that republishes this replacement.
            /// </summary>
            private readonly StagedHistorianProviderRegistry m_owner;

            /// <summary>
            /// The replacement transaction owned by the candidate registry.
            /// </summary>
            private readonly IHistorianNodeReplacement m_replacement;
        }

        /// <summary>
        /// Serializes candidate changes and publication.
        /// </summary>
        private readonly Lock m_lock = new();

        /// <summary>
        /// Namespace table used to resolve candidate node namespaces.
        /// </summary>
        private readonly NamespaceTable m_namespaceTable;

        /// <summary>
        /// Registry containing this candidate's local bindings.
        /// </summary>
        private readonly HistorianProviderRegistry m_candidateRegistry;

        /// <summary>
        /// Server registry to which this candidate publishes.
        /// </summary>
        private readonly IHistorianProviderRegistry m_servingRegistry;

        /// <summary>
        /// Stable identity used to own this candidate's serving registrations.
        /// </summary>
        private readonly object m_owner;

        /// <summary>
        /// Node scopes retained from the serving registry during candidate configuration.
        /// </summary>
        private readonly HashSet<NodeId> m_retainedNodeIds = [];

        /// <summary>
        /// Namespace scopes retained from the serving registry during candidate configuration.
        /// </summary>
        private readonly HashSet<string> m_retainedNamespaceUris = new(StringComparer.Ordinal);

        /// <summary>
        /// Whether the candidate has published its registration set.
        /// </summary>
        private bool m_published;

        /// <summary>
        /// Whether this candidate currently contributes registrations to the serving registry.
        /// </summary>
        private bool m_servingHasRegistrations;

        /// <summary>
        /// Whether the serving default registration is retained by this candidate.
        /// </summary>
        private bool m_retainedDefault;

        /// <summary>
        /// Whether this candidate has left routing.
        /// </summary>
        private bool m_retired;

        /// <summary>
        /// Whether this candidate registry has been disposed.
        /// </summary>
        private bool m_disposed;
    }
}
