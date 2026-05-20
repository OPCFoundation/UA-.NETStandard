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
    /// thread-safe (via <see cref="SemaphoreSlim"/>) live dispatcher.
    /// </summary>
    public sealed class AliasNameStoreRegistry : IAliasNameStoreRegistry, IDisposable
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
                m_semaphore.Wait();
                try
                {
                    return [.. m_stores];
                }
                finally
                {
                    m_semaphore.Release();
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

            m_semaphore.Wait();
            try
            {
                if (m_stores.Contains(store))
                {
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
                foreach (NodeId id in categoryIds)
                {
                    m_categoryToStore[id] = store;
                }
                m_stores.Add(store);
                store.Changed += OnStoreChanged;
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public void Unregister(IAliasNameStore store)
        {
            if (store == null)
            {
                return;
            }

            m_semaphore.Wait();
            try
            {
                if (!m_stores.Remove(store))
                {
                    return;
                }
                store.Changed -= OnStoreChanged;
                CollectCategoryIds(store, out HashSet<NodeId> categoryIds);
                foreach (NodeId id in categoryIds)
                {
                    if (m_categoryToStore.TryGetValue(id, out IAliasNameStore? owner)
                        && ReferenceEquals(owner, store))
                    {
                        m_categoryToStore.Remove(id);
                    }
                }
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public IAliasNameStore? GetStoreForCategory(NodeId categoryId)
        {
            if (categoryId.IsNull)
            {
                return null;
            }
            m_semaphore.Wait();
            try
            {
                m_categoryToStore.TryGetValue(categoryId, out IAliasNameStore? store);
                return store;
            }
            finally
            {
                m_semaphore.Release();
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
            IAliasNameStore? store = GetStoreForCategory(categoryId);
            if (store == null)
            {
                return (new ServiceResult(StatusCodes.BadNotImplemented), []);
            }
            IReadOnlyList<AliasNameDataType> aliases = await store
                .FindAliasAsync(categoryId, aliasNameSearchPattern,
                    referenceTypeFilter, typeTree, ct)
                .ConfigureAwait(false);
            return (ServiceResult.Good, aliases);
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
            IAliasNameStore? store = GetStoreForCategory(categoryId);
            if (store == null)
            {
                return (new ServiceResult(StatusCodes.BadNotImplemented), []);
            }
            IReadOnlyList<AliasNameVerboseDataType> aliases = await store
                .FindAliasVerboseAsync(categoryId, aliasNameSearchPattern,
                    referenceTypeFilter, typeTree, ct)
                .ConfigureAwait(false);
            return (ServiceResult.Good, aliases);
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
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_semaphore.Wait();
            try
            {
                foreach (IAliasNameStore store in m_stores)
                {
                    store.Changed -= OnStoreChanged;
                }
                m_stores.Clear();
                m_categoryToStore.Clear();
            }
            finally
            {
                m_semaphore.Release();
            }
            m_semaphore.Dispose();
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

        private readonly List<IAliasNameStore> m_stores = [];
        private readonly Dictionary<NodeId, IAliasNameStore> m_categoryToStore = [];
        private readonly SemaphoreSlim m_semaphore = new(1, 1);
        private bool m_disposed;
    }
}
