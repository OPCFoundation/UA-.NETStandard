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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Default <see cref="IDistributedValueCache"/> over an
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
        public DistributedValueCache(INodeStateStore store, TimeProvider? timeProvider = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_time = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ValueTask CacheAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default)
        {
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
    }
}
