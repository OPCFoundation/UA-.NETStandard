// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: default <see cref="IDistributedValueCache"/> over an
    /// <see cref="INodeStateStore"/>. Freshness is evaluated against the
    /// value's <see cref="DataValue.SourceTimestamp"/> using an injectable
    /// <see cref="TimeProvider"/>.
    /// </summary>
    public sealed class DistributedValueCache : IDistributedValueCache
    {
        /// <summary>
        /// Creates a value cache over a node state store.
        /// </summary>
        /// <param name="store">The backing node state store.</param>
        /// <param name="timeProvider">Time source (defaults to system).</param>
        /// <param name="isWriter">
        /// Optional predicate that decides whether cache writes are allowed.
        /// When supplied and it returns <c>false</c>, <see cref="CacheAsync"/>
        /// becomes a no-op so standby replicas can participate as read-only
        /// consumers of the shared cache.
        /// </param>
        public DistributedValueCache(
            INodeStateStore store,
            TimeProvider? timeProvider = null,
            Func<bool>? isWriter = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_time = timeProvider ?? TimeProvider.System;
            m_isWriter = isWriter;
        }

        /// <inheritdoc/>
        public ValueTask CacheAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default)
        {
            if (m_isWriter != null && !m_isWriter())
            {
                return default;
            }

            return m_store.WriteValueAsync(nodeId, value, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<(bool Fresh, DataValue Value)> TryGetAsync(
            NodeId nodeId,
            TimeSpan maxAge,
            CancellationToken ct = default)
        {
            (bool found, DataValue value) = await m_store
                .TryReadValueAsync(nodeId, ct)
                .ConfigureAwait(false);
            if (!found)
            {
                return (false, DataValue.Null);
            }

            DateTimeUtc now = m_time.GetUtcNow();
            // A missing timestamp (MinValue) yields a huge age and is treated
            // as stale; clock skew into the future yields a negative age and
            // is treated as fresh.
            bool fresh = (now - value.SourceTimestamp) <= maxAge;
            return (fresh, value);
        }

        private readonly INodeStateStore m_store;
        private readonly TimeProvider m_time;
        private readonly Func<bool>? m_isWriter;
    }
}
