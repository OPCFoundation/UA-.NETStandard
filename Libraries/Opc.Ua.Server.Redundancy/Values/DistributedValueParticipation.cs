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

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: helpers that let a variable's read/write callbacks participate in the
    /// distributed value cache: serve the last value with a freshness bound
    /// and cache values on read/write. Monitored items are unaffected — they
    /// continue to read through the normal pipeline and therefore observe the
    /// cached value only when the read path participates.
    /// </summary>
    public static class DistributedValueParticipation
    {
        /// <summary>
        /// Returns the cached value when it is fresh; otherwise invokes
        /// <paramref name="liveRead"/>, caches the result, and returns it.
        /// Use from a custom read callback.
        /// </summary>
        /// <param name="cache">The distributed value cache.</param>
        /// <param name="nodeId">The variable node identifier.</param>
        /// <param name="maxAge">The freshness bound for the cached value.</param>
        /// <param name="liveRead">Reads the live value from the source.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async ValueTask<DataValue> ReadThroughAsync(
            IDistributedValueCache cache,
            NodeId nodeId,
            TimeSpan maxAge,
            Func<CancellationToken, ValueTask<DataValue>> liveRead,
            CancellationToken ct = default)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }
            if (liveRead == null)
            {
                throw new ArgumentNullException(nameof(liveRead));
            }

            (bool fresh, DataValue cached) = await cache.TryGetAsync(nodeId, maxAge, ct).ConfigureAwait(false);
            if (fresh)
            {
                return cached;
            }

            DataValue live = await liveRead(ct).ConfigureAwait(false);
            await cache.CacheAsync(nodeId, live, ct).ConfigureAwait(false);
            return live;
        }

        /// <summary>
        /// Wires a variable's asynchronous read/write callbacks to the
        /// distributed value cache: reads serve the last value while fresh
        /// (falling back to <paramref name="liveRead"/> and caching), and
        /// writes are cached (write-through).
        /// </summary>
        /// <param name="variable">The variable to wire.</param>
        /// <param name="cache">The distributed value cache.</param>
        /// <param name="maxAge">The freshness bound for cached reads.</param>
        /// <param name="liveRead">Reads the live value from the source.</param>
        public static void EnableDistributedValueParticipation(
            this BaseVariableState variable,
            IDistributedValueCache cache,
            TimeSpan maxAge,
            Func<CancellationToken, ValueTask<DataValue>> liveRead)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }
            if (liveRead == null)
            {
                throw new ArgumentNullException(nameof(liveRead));
            }

            NodeId nodeId = variable.NodeId;

            variable.OnReadValueAsync = async (context, node, indexRange, dataEncoding, ct) =>
            {
                DataValue value = await ReadThroughAsync(cache, nodeId, maxAge, liveRead, ct).ConfigureAwait(false);
                return new AttributeReadResult(
                    ServiceResult.Good,
                    value.WrappedValue,
                    value.StatusCode,
                    value.SourceTimestamp);
            };

            variable.OnWriteValueAsync = async (context, node, indexRange, value, ct) =>
            {
                var dataValue = new DataValue(value, StatusCodes.Good, DateTimeUtc.Now);
                await cache.CacheAsync(nodeId, dataValue, ct).ConfigureAwait(false);
                return new AttributeWriteResult(ServiceResult.Good);
            };
        }
    }
}
