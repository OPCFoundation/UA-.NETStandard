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

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Default <see cref="IAliasNameStoreRegistry"/> implementation —
    /// thread-safe live dispatcher.
    /// </summary>
    public sealed class AliasNameStoreRegistry :
        IAliasNameStoreRegistry, IAliasNameStoreRegistrationProvider, IDisposable
    {
        /// <summary>
        /// Initializes a new, empty registry.
        /// </summary>
        public AliasNameStoreRegistry()
        {
        }

        /// <inheritdoc/>
        public IReadOnlyList<IAliasNameStore> Stores
        {
            get
            {
                lock (m_lock)
                {
                    ThrowIfDisposed();
                    return [.. m_stores];
                }
            }
        }

        /// <inheritdoc/>
        public event EventHandler<AliasStoreChangedEventArgs>? Changed;

        /// <inheritdoc/>
        public void Register(IAliasNameStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                if (m_stores.Contains(store))
                {
                    m_manualStores.Add(store);
                    return;
                }
                CollectCategoryIds(store, out HashSet<NodeId> categoryIds);
                foreach (NodeId id in categoryIds)
                {
                    if (m_categoryToStore.ContainsKey(id))
                    {
                        throw new InvalidOperationException(
                            "Another alias-name store already owns category " + id);
                    }
                }
                m_manualStores.Add(store);
                AddStore(store, categoryIds);
            }
        }

        /// <inheritdoc />
        public IDisposable AcquireRegistration(IAliasNameStore store, Guid ownerId)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            if (ownerId == Guid.Empty)
            {
                throw new ArgumentException("An alias-store owner must have a stable identity.", nameof(ownerId));
            }
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_ownedRegistrations.TryGetValue(ownerId, out StoreRegistration? previous);
                CollectCategoryIds(store, out HashSet<NodeId> categoryIds);
                foreach (NodeId id in categoryIds)
                {
                    if (m_categoryToStore.TryGetValue(id, out IAliasNameStore? owner) &&
                        !ReferenceEquals(owner, store) &&
                        (previous is null || !ReferenceEquals(previous.Store, owner) ||
                            IsClaimedByAnotherOwner(owner, ownerId)))
                    {
                        throw new InvalidOperationException(
                            "Another alias-name store already owns category " + id);
                    }
                }
                var registration = new StoreRegistration(this, store, ownerId);
                m_ownedRegistrations[ownerId] = registration;
                if (previous is not null && !ReferenceEquals(previous.Store, store))
                {
                    RemoveStoreIfUnclaimed(previous.Store);
                }
                if (!m_stores.Contains(store))
                {
                    AddStore(store, categoryIds);
                }
                return registration;
            }
        }

        /// <inheritdoc/>
        public void Unregister(IAliasNameStore store)
        {
            if (store == null)
            {
                return;
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                m_manualStores.Remove(store);
                RemoveStoreIfUnclaimed(store);
            }
        }

        /// <inheritdoc/>
        public void RegisterContributor(IAliasNameStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                if (m_contributors.Contains(store))
                {
                    return;
                }
                m_contributors.Add(store);
                store.Changed += OnStoreChanged;
            }
        }

        /// <inheritdoc/>
        public void UnregisterContributor(IAliasNameStore store)
        {
            if (store == null)
            {
                return;
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                if (m_contributors.Remove(store))
                {
                    store.Changed -= OnStoreChanged;
                }
            }
        }

        /// <inheritdoc/>
        public IAliasNameStore? GetStoreForCategory(NodeId categoryId)
        {
            if (categoryId.IsNull)
            {
                return null;
            }
            lock (m_lock)
            {
                ThrowIfDisposed();
                m_categoryToStore.TryGetValue(categoryId, out IAliasNameStore? store);
                return store;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(ServiceResult Result, IReadOnlyList<AliasNameDataType> Aliases)>
            DispatchFindAliasAsync(
                NodeId categoryId,
                string? aliasNameSearchPattern,
                NodeId referenceTypeFilter,
                ITypeTable typeTree,
                CancellationToken ct = default)
        {
            List<IAliasNameStore> stores = GetQueryStores(categoryId);
            if (stores.Count == 0)
            {
                return (new ServiceResult(StatusCodes.BadNotImplemented), []);
            }
            try
            {
                ValidateSearchPattern(aliasNameSearchPattern);
                if (stores.Count == 1)
                {
                    return (ServiceResult.Good, await stores[0]
                        .FindAliasAsync(categoryId, aliasNameSearchPattern,
                            referenceTypeFilter, typeTree, ct)
                        .ConfigureAwait(false));
                }
                var aliases = new List<AliasNameDataType>();
                foreach (IAliasNameStore store in stores)
                {
                    aliases.AddRange(await store
                        .FindAliasAsync(categoryId, aliasNameSearchPattern,
                            referenceTypeFilter, typeTree, ct)
                        .ConfigureAwait(false));
                }
                return (ServiceResult.Good, aliases);
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadInvalidArgument || ex.StatusCode == StatusCodes.BadTimeout)
            {
                return (ex.Result, []);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(ServiceResult Result, IReadOnlyList<AliasNameVerboseDataType> Aliases)>
            DispatchFindAliasVerboseAsync(
                NodeId categoryId,
                string? aliasNameSearchPattern,
                NodeId referenceTypeFilter,
                ITypeTable typeTree,
                CancellationToken ct = default)
        {
            List<IAliasNameStore> stores = GetQueryStores(categoryId);
            if (stores.Count == 0)
            {
                return (new ServiceResult(StatusCodes.BadNotImplemented), []);
            }
            try
            {
                ValidateSearchPattern(aliasNameSearchPattern);
                if (stores.Count == 1)
                {
                    return (ServiceResult.Good, await stores[0]
                        .FindAliasVerboseAsync(categoryId, aliasNameSearchPattern,
                            referenceTypeFilter, typeTree, ct)
                        .ConfigureAwait(false));
                }
                var aliases = new List<AliasNameVerboseDataType>();
                foreach (IAliasNameStore store in stores)
                {
                    aliases.AddRange(await store
                        .FindAliasVerboseAsync(categoryId, aliasNameSearchPattern,
                            referenceTypeFilter, typeTree, ct)
                        .ConfigureAwait(false));
                }
                return (ServiceResult.Good, aliases);
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadInvalidArgument || ex.StatusCode == StatusCodes.BadTimeout)
            {
                return (ex.Result, []);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(ServiceResult Result, StatusCode[] ErrorCodes)>
            DispatchAddAliasesAsync(
                NodeId categoryId,
                IReadOnlyList<AliasAddRequest> requests,
                CancellationToken ct = default)
        {
            IAliasNameStore? store = GetStoreForCategory(categoryId);
            if (store == null)
            {
                return (new ServiceResult(StatusCodes.BadNotImplemented), []);
            }
            try
            {
                StatusCode[] codes = await store
                    .AddAliasesAsync(categoryId, requests, ct)
                    .ConfigureAwait(false);
                return (ServiceResult.Good, codes);
            }
            catch (ServiceResultException sre)
            {
                return (sre.Result, []);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(ServiceResult Result, StatusCode[] ErrorCodes)>
            DispatchDeleteAliasesAsync(
                NodeId categoryId,
                IReadOnlyList<AliasDeleteRequest> requests,
                CancellationToken ct = default)
        {
            IAliasNameStore? store = GetStoreForCategory(categoryId);
            if (store == null)
            {
                return (new ServiceResult(StatusCodes.BadNotImplemented), []);
            }
            try
            {
                StatusCode[] codes = await store
                    .DeleteAliasesAsync(categoryId, requests, ct)
                    .ConfigureAwait(false);
                return (ServiceResult.Good, codes);
            }
            catch (ServiceResultException sre)
            {
                return (sre.Result, []);
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
                m_disposed = true;
                foreach (IAliasNameStore store in m_stores)
                {
                    store.Changed -= OnStoreChanged;
                }
                foreach (IAliasNameStore store in m_contributors)
                {
                    store.Changed -= OnStoreChanged;
                }
                m_stores.Clear();
                m_contributors.Clear();
                m_categoryToStore.Clear();
                m_manualStores.Clear();
                m_ownedRegistrations.Clear();
            }
        }

        /// <summary>
        /// Rejects malformed nonempty wildcard patterns before dispatching an alias search.
        /// </summary>
        /// <summary>
        /// The stores a query on <paramref name="categoryId"/> fans out
        /// to: the owner first, then every contributor reporting the
        /// category.
        /// </summary>
        private List<IAliasNameStore> GetQueryStores(NodeId categoryId)
        {
            var stores = new List<IAliasNameStore>();
            if (categoryId.IsNull)
            {
                return stores;
            }
            lock (m_lock)
            {
                ThrowIfDisposed();
                if (m_categoryToStore.TryGetValue(categoryId, out IAliasNameStore? owner))
                {
                    stores.Add(owner);
                }
                foreach (IAliasNameStore contributor in m_contributors)
                {
                    if (contributor.OwnsCategory(categoryId))
                    {
                        stores.Add(contributor);
                    }
                }
            }
            return stores;
        }

        private static void ValidateSearchPattern(string? pattern)
        {
            if (!string.IsNullOrEmpty(pattern))
            {
                _ = AliasNameWildcardMatcher.CreatePattern(pattern);
            }
        }

        private void OnStoreChanged(object? sender, AliasStoreChangedEventArgs e)
        {
            Changed?.Invoke(this, e);
        }

        private static void CollectCategoryIds(
            IAliasNameStore store,
            out HashSet<NodeId> categoryIds)
        {
            categoryIds = [];
            foreach (AliasNameCategoryDescriptor root in store.RootCategories)
            {
                AddRecursive(root, categoryIds);
            }

            static void AddRecursive(
                AliasNameCategoryDescriptor descriptor,
                HashSet<NodeId> sink)
            {
                sink.Add(descriptor.NodeId);
                foreach (AliasNameCategoryDescriptor child in descriptor.SubCategories)
                {
                    AddRecursive(child, sink);
                }
            }
        }

        /// <summary>
        /// Adds one validated store and subscribes to its changes once.
        /// </summary>
        private void AddStore(IAliasNameStore store, HashSet<NodeId> categoryIds)
        {
            foreach (NodeId id in categoryIds)
            {
                m_categoryToStore[id] = store;
            }
            m_stores.Add(store);
            store.Changed += OnStoreChanged;
        }

        /// <summary>
        /// Determines whether a manual registration or another owner still claims a store.
        /// </summary>
        private bool IsClaimedByAnotherOwner(IAliasNameStore store, Guid ownerId)
        {
            if (m_manualStores.Contains(store))
            {
                return true;
            }
            foreach (KeyValuePair<Guid, StoreRegistration> entry in m_ownedRegistrations)
            {
                if (entry.Key != ownerId && ReferenceEquals(entry.Value.Store, store))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes a store only after its last current claim has been released.
        /// </summary>
        private void RemoveStoreIfUnclaimed(IAliasNameStore store)
        {
            if (IsClaimedByAnotherOwner(store, Guid.Empty) || !m_stores.Remove(store))
            {
                return;
            }
            store.Changed -= OnStoreChanged;
            CollectCategoryIds(store, out HashSet<NodeId> categoryIds);
            foreach (NodeId id in categoryIds)
            {
                if (m_categoryToStore.TryGetValue(id, out IAliasNameStore? owner) &&
                    ReferenceEquals(owner, store))
                {
                    m_categoryToStore.Remove(id);
                }
            }
        }

        /// <summary>
        /// Releases a current claim without disturbing a replacement or another owner.
        /// </summary>
        private void ReleaseRegistration(StoreRegistration registration)
        {
            lock (m_lock)
            {
                if (!m_disposed &&
                    m_ownedRegistrations.TryGetValue(registration.OwnerId, out StoreRegistration? current) &&
                    ReferenceEquals(current, registration))
                {
                    m_ownedRegistrations.Remove(registration.OwnerId);
                    RemoveStoreIfUnclaimed(registration.Store);
                }
            }
        }

        /// <summary>
        /// Rejects operations after the registry has released its stores.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(AliasNameStoreRegistry));
            }
        }

        /// <summary>
        /// Owns one registration claim, which can be superseded without retaining it in the registry.
        /// </summary>
        private sealed class StoreRegistration : IDisposable
        {
            /// <summary>
            /// Initializes a registration owned by one stable identity.
            /// </summary>
            public StoreRegistration(AliasNameStoreRegistry registry, IAliasNameStore store, Guid ownerId)
            {
                m_registry = registry;
                Store = store;
                OwnerId = ownerId;
            }

            /// <summary>
            /// Gets the store associated with this claim.
            /// </summary>
            public IAliasNameStore Store { get; }

            /// <summary>
            /// Gets the stable identity shared by successive registrations.
            /// </summary>
            public Guid OwnerId { get; }

            /// <inheritdoc />
            public void Dispose()
            {
                Interlocked.Exchange(ref m_registry, null)?.ReleaseRegistration(this);
            }

            private AliasNameStoreRegistry? m_registry;
        }

        private readonly List<IAliasNameStore> m_stores = [];
        private readonly List<IAliasNameStore> m_contributors = [];
        private readonly Dictionary<NodeId, IAliasNameStore> m_categoryToStore = [];
        private readonly HashSet<IAliasNameStore> m_manualStores = [];
        private readonly Dictionary<Guid, StoreRegistration> m_ownedRegistrations = [];
        private readonly Lock m_lock = new();
        private bool m_disposed;
    }
}
