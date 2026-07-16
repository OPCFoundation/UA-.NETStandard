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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// An <see cref="IDistributedValueCache"/> that routes each node to the
    /// <see cref="INodeStateStore"/> resolved from the server's
    /// <see cref="INodeStateStoreRegistry"/> (exact NodeId, then namespace, then
    /// the default store).
    /// </summary>
    /// <remarks>
    /// The distributed node-state store is built with the server message context
    /// when the server starts, so the registry is not available at
    /// dependency-injection registration time. The registry accessor is therefore
    /// evaluated on every call rather than captured at construction, which lets
    /// this cache be injected as a singleton and used by node managers once the
    /// server is running.
    /// </remarks>
    internal sealed class RegistryBackedDistributedValueCache : IDistributedValueCache
    {
        /// <summary>
        /// Creates the registry-backed cache.
        /// </summary>
        /// <param name="registryAccessor">
        /// Returns the node-state store registry, or <c>null</c> before the
        /// server has started.
        /// </param>
        /// <param name="timeProvider">Time source (defaults to system).</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registryAccessor"/> is <c>null</c>.
        /// </exception>
        public RegistryBackedDistributedValueCache(
            Func<INodeStateStoreRegistry?> registryAccessor,
            TimeProvider? timeProvider = null)
        {
            m_registryAccessor = registryAccessor ?? throw new ArgumentNullException(nameof(registryAccessor));
            m_time = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ValueTask CacheAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default)
        {
            return ResolveCache(nodeId).CacheAsync(nodeId, value, ct);
        }

        /// <inheritdoc/>
        public ValueTask<(bool Fresh, DataValue Value)> TryGetAsync(
            NodeId nodeId,
            TimeSpan maxAge,
            CancellationToken ct = default)
        {
            return ResolveCache(nodeId).TryGetAsync(nodeId, maxAge, ct);
        }

        private DistributedValueCache ResolveCache(NodeId nodeId)
        {
            INodeStateStoreRegistry registry = m_registryAccessor()
                ?? throw new InvalidOperationException(
                    "The distributed node-state store is not available yet. IDistributedValueCache can only be " +
                    "used after the server has started; the store is built with the server message context in the " +
                    "distributed address-space startup task.");

            INodeStateStore store = registry.Resolve(nodeId)
                ?? throw new InvalidOperationException(
                    $"No distributed node-state store is registered for node '{nodeId}'. Configure a distributed " +
                    "address space with UseDistributedAddressSpace so a default store is registered.");

            return m_caches.GetOrAdd(store, s => new DistributedValueCache(s, m_time));
        }

        private readonly Func<INodeStateStoreRegistry?> m_registryAccessor;
        private readonly TimeProvider m_time;
        private readonly ConcurrentDictionary<INodeStateStore, DistributedValueCache> m_caches = new();
    }
}
