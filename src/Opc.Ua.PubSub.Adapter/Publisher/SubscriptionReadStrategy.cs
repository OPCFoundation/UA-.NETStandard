/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Publisher
{
    /// <summary>
    /// An <see cref="IReadStrategy"/> that serves the publisher's
    /// per-cycle reads from a latest-value cache. The cache is keyed by node and
    /// attribute and index range and is kept up to date by an
    /// <see cref="IDataChangeSubscription"/> whose monitored items push
    /// data changes through <see cref="IDataChangeSubscription.DataChanged"/>.
    /// Reads never touch the network: they sample the cache and return the most
    /// recent value, or an uncertain placeholder for keys not yet primed.
    /// </summary>
    public sealed class SubscriptionReadStrategy : IReadStrategy, IDisposable
    {
        /// <summary>
        /// Creates a new subscription-backed read strategy.
        /// </summary>
        /// <param name="telemetry">
        /// The telemetry context used to create the logger.
        /// </param>
        /// <param name="maxCacheEntries">
        /// The maximum number of node/attribute/range entries the cache retains. New
        /// keys beyond this bound are dropped and a warning is logged once.
        /// </param>
        public SubscriptionReadStrategy(
            ITelemetryContext telemetry,
            int maxCacheEntries = DefaultMaxCacheEntries)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<SubscriptionReadStrategy>();
            m_maxCacheEntries = maxCacheEntries > 0 ? maxCacheEntries : DefaultMaxCacheEntries;
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ThrowIfDisposed();

                int count = nodesToRead.IsNull ? 0 : nodesToRead.Count;
                var results = new DataValue[count];
                for (int i = 0; i < count; i++)
                {
                    ReadValueId nodeToRead = nodesToRead[i];
                    ServiceResult rangeResult = NumericRange.Validate(
                        nodeToRead?.IndexRange ?? string.Empty, out NumericRange range);
                    if (ServiceResult.IsBad(rangeResult))
                    {
                        results[i] = DataValue.FromStatusCode(rangeResult.StatusCode);
                    }
                    else if (nodeToRead?.NodeId is { IsNull: false } nodeId &&
                        m_cache.TryGetValue(
                            new NodeAttributeKey(nodeId, NormalizeAttribute(nodeToRead.AttributeId), range.ToString()),
                            out DataValue value))
                    {
                        // The server has already applied this exact range, including its status and timestamps.
                        results[i] = value;
                    }
                    else
                    {
                        results[i] = DataValue.FromStatusCode(StatusCodes.UncertainInitialValue);
                    }
                }
                return new ValueTask<ArrayOf<DataValue>>(results.ToArrayOf());
            }
        }

        /// <summary>
        /// Attaches the supplied subscription so its data-change notifications
        /// update the cache. Only one subscription may be attached; attaching a
        /// second one replaces the first.
        /// </summary>
        /// <param name="subscription">
        /// The data-change subscription feeding this cache.
        /// </param>
        /// <exception cref="ArgumentNullException"></exception>
        internal void Attach(IDataChangeSubscription subscription)
        {
            if (subscription is null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }
            lock (m_gate)
            {
                ThrowIfDisposed();
                if (ReferenceEquals(m_subscription, subscription))
                {
                    return;
                }
                m_subscription?.DataChanged -= OnDataChanged;
                m_cache.Clear();
                m_handleToKey.Clear();
                m_subscription = subscription;
                subscription.DataChanged += OnDataChanged;
            }
        }

        /// <summary>
        /// Records the mapping from a monitored item's client handle to its
        /// node/attribute/range cache key and seeds an uncertain placeholder so the key
        /// is resolvable before the first data change or prime arrives.
        /// </summary>
        /// <param name="clientHandle">
        /// The client handle returned by
        /// <see cref="IDataChangeSubscription.AddMonitoredItemAsync"/>.
        /// </param>
        /// <param name="nodeId">
        /// The monitored node identifier.
        /// </param>
        /// <param name="attributeId">
        /// The monitored attribute identifier.
        /// </param>
        /// <param name="indexRange">The exact server-side range associated with the handle.</param>
        internal void RegisterMonitoredItem(
            uint clientHandle,
            NodeId nodeId,
            uint attributeId,
            string? indexRange = null)
        {
            if (nodeId.IsNull)
            {
                return;
            }
            lock (m_gate)
            {
                ThrowIfDisposed();
                var key = new NodeAttributeKey(
                    nodeId, NormalizeAttribute(attributeId), NormalizeRange(indexRange));
                m_handleToKey[clientHandle] = key;
                if (m_cache.Count < m_maxCacheEntries)
                {
                    m_cache.TryAdd(key, DataValue.FromStatusCode(StatusCodes.UncertainInitialValue));
                }
                else
                {
                    LogCacheFull();
                }
            }
        }

        /// <summary>
        /// Seeds or refreshes the cached projection for the supplied node/attribute/range,
        /// typically from a one-shot priming Read before the first publish cycle.
        /// </summary>
        /// <param name="nodeId">
        /// The node identifier whose value is being seeded.
        /// </param>
        /// <param name="attributeId">
        /// The attribute identifier whose value is being seeded.
        /// </param>
        /// <param name="value">
        /// The value to store in the cache.
        /// </param>
        /// <param name="indexRange">The exact server-side range requested by the priming read.</param>
        internal void Seed(NodeId nodeId, uint attributeId, in DataValue value, string? indexRange = null)
        {
            if (nodeId.IsNull)
            {
                return;
            }
            lock (m_gate)
            {
                ThrowIfDisposed();
                Store(new NodeAttributeKey(nodeId, NormalizeAttribute(attributeId), NormalizeRange(indexRange)), value);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_subscription?.DataChanged -= OnDataChanged;
                m_subscription = null;
                m_cache.Clear();
                m_handleToKey.Clear();
            }
        }

        private void OnDataChanged(object? sender, DataChangeEventArgs e)
        {
            lock (m_gate)
            {
                if (m_disposed || e is null || !ReferenceEquals(sender, m_subscription))
                {
                    return;
                }
                if (m_handleToKey.TryGetValue(e.ClientHandle, out NodeAttributeKey key))
                {
                    Store(key, e.Value);
                }
            }
        }

        private void Store(in NodeAttributeKey key, in DataValue value)
        {
            if (m_cache.ContainsKey(key) || m_cache.Count < m_maxCacheEntries)
            {
                m_cache[key] = value;
            }
            else
            {
                LogCacheFull();
            }
        }

        private void LogCacheFull()
        {
            // TODO: when the session signals a disconnect the cache should be
            // dropped/refreshed; until then a saturated cache only refuses new
            // keys and is reported once to avoid log spam.
            if (Interlocked.Exchange(ref m_cacheFullLogged, 1) == 0)
            {
                m_logger.ExternalSubscriptionCacheFull(m_maxCacheEntries);
            }
        }

        private static uint NormalizeAttribute(uint attributeId)
        {
            return attributeId != 0 ? attributeId : Attributes.Value;
        }

        /// <summary>
        /// Canonicalizes equivalent valid ranges without treating an invalid range as the whole value.
        /// </summary>
        private static string NormalizeRange(string? indexRange)
        {
            return NumericRange.Parse(indexRange ?? string.Empty).ToString();
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(SubscriptionReadStrategy));
            }
        }

        /// <summary>
        /// Immutable cache key identifying one projection of a node attribute.
        /// </summary>
        private readonly struct NodeAttributeKey : IEquatable<NodeAttributeKey>
        {
            public NodeAttributeKey(NodeId nodeId, uint attributeId, string indexRange)
            {
                m_nodeId = nodeId;
                m_attributeId = attributeId;
                m_indexRange = indexRange;
            }

            public bool Equals(NodeAttributeKey other)
            {
                return m_attributeId == other.m_attributeId &&
                    m_nodeId.Equals(other.m_nodeId) &&
                    string.Equals(m_indexRange, other.m_indexRange, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is NodeAttributeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(m_nodeId, m_attributeId, m_indexRange);
            }

            private readonly NodeId m_nodeId;
            private readonly uint m_attributeId;

            /// <summary>The projection already applied by the server.</summary>
            private readonly string m_indexRange;
        }

        private const int DefaultMaxCacheEntries = 100_000;

        private readonly ILogger m_logger;
        private readonly int m_maxCacheEntries;

        /// <summary>Serializes notification admission, cache reads, and retirement.</summary>
        private readonly Lock m_gate = new();
        private readonly ConcurrentDictionary<NodeAttributeKey, DataValue> m_cache = new();
        private readonly ConcurrentDictionary<uint, NodeAttributeKey> m_handleToKey = new();
        private IDataChangeSubscription? m_subscription;
        private int m_cacheFullLogged;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for SubscriptionReadStrategy.
    /// </summary>
    internal static partial class SubscriptionReadStrategyLog
    {
        [LoggerMessage(EventId = PubSubAdapterEventIds.SubscriptionReadStrategy + 0, Level = LogLevel.Warning,
            Message = "External subscription latest-value cache reached its bound of {MaxEntries} entries; " +
                "new node/attribute/range keys are dropped.")]
        public static partial void ExternalSubscriptionCacheFull(this ILogger logger, int maxEntries);
    }

}
